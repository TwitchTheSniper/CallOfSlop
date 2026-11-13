using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a <see cref="CActKind.Faction"/> asks for (its op byte).</summary>
public enum FactionOp : byte
{
    Found, // text: start a faction of your own with this name (you lead it)
    Invite, // u8 player: the leader invites someone who is here
    Accept, // u8 faction: join the faction you were invited to (leaving your old one)
    Decline, // turn the invite down
    Leave, // leave your faction (a leader hands over; the last one out ends it)
    Remove, // u8 owner byte: the leader takes a member out
    GiveBody, // u16 body, u8 owner byte: give a construct you own to a member
    GiveHeld, // u8 player: hand the part in your hands to a member near you
    Looks, // u32 colour (0xRRGGBB), text tag, u8 open: the faction's colour, tag and whether anyone may join (FactionMay.Looks)
    Join, // u8 faction: join an open faction, or ask to join one that is invite only
    Answer, // u8 player, u8 yes: let in (or turn away) someone who asked to join (FactionMay.Invite)
    SetRank, // u8 owner byte, u8 rank: move a member of a lower rank to another below one's own (FactionMay.Promote)
    MakeLeader, // u8 owner byte: the leader hands the faction to another member (and becomes the rank below)
    Stance, // u8 faction, i8 stance (-1 war, 0 neutral, 1 alliance): toward another faction (FactionMay.Relations)
}

/// <summary>
/// Sandbox player factions on the server (Source/Constructs/Factions.cs, docs/sandbox/CONSTRUCTS.md "Factions"): found
/// one, invite people, accept, leave, remove, and give a construct or the part in your hands to a member. Everything a
/// member owns is shared with the others (<see cref="Factions.Shares"/>). The table goes to everyone whole
/// (<see cref="Msg.Factions"/>) when it changes and to each joiner; a saved world keeps who is in which (GameWorldSave.cs).
/// </summary>
public sealed partial class Game
{
    /// <summary>This Game's factions (its world's <see cref="FactionTable"/>).</summary>
    public FactionTable FactionTable => Place.Factions;

    /// <summary>How near a member must be for the part in your hands to be handed over (metres).</summary>
    public const float FactionHandReach = 8f;

    private int _factionSent = -1;

    /// <summary>A person (not a bot or one of the story's people), who may be in a faction.</summary>
    public static bool FactionPerson(Player p) => p.Bot == null && p.Team is not (SbFriendlyTeam or SbHostileTeam or SbPeopleTeam);

    /// <summary>A member's name by owner byte: the player if here, else the name kept for them.</summary>
    public string FactionMemberName(byte owner) =>
        owner < MaxPlayers && Players.TryGetValue(owner, out var p) ? p.Name : FactionTable.MemberName[owner] is { Length: > 0 } n ? n : $"player {owner}";

    // ---- The server -----------------------------------------------------------------------------------------------

    /// <summary>Server: one faction action from a player (dead or alive). Null, or why not; what happened goes out as notices.</summary>
    private string? SandboxFactionAct(Player p, ref PacketReader r)
    {
        var t = FactionTable;
        var me = p.Id;
        var mine = t.Of[me];
        if (!FactionPerson(p))
            return "only players can be in a faction";
        switch ((FactionOp)r.U8())
        {
            case FactionOp.Found:
            {
                var name = FactionTable.CleanName(r.Text(64));
                if (mine != 0)
                    return $"you are already in {t.Names[mine]}: leave it first";
                if (name.Length < 2)
                    return "a faction needs a name (2 to 24 letters)";
                if (t.Find(name) != 0)
                    return $"there is already a faction called {name}";
                var id = t.FreeId();
                if (id == 0)
                    return "there are as many factions as there can be";
                t.Names[id] = name;
                t.Leader[id] = me;
                t.Of[me] = id;
                t.Rank[me] = (byte)FactionRanks.Player.Top;
                t.MemberName[me] = p.Name;
                t.Invited[me] = 0;
                t.Asked[me] = 0;
                t.Colour[id] = FactionTable.ColourFrom(name);
                t.SetTag(id, FactionTable.TagFrom(name));
                t.Open[id] = false;
                t.Version++;
                FactionTell(me, $"You founded {name}. Pick its colour and tag and invite people from the faction window.");
                StoryFactionFounded(p, name); // (the story hears of a new outfit: GameStoryFactions.cs)
                SandboxHands.Done.Add($"player {me} founded faction {id} \"{name}\"");
                return null;
            }
            case FactionOp.Invite:
            {
                var who = r.U8();
                if (mine == 0)
                    return "you are not in a faction: found one first";
                if (!t.May(me, FactionMay.Invite))
                    return $"your rank ({RankName(me)}) can't invite people";
                if (!Players.TryGetValue(who, out var them) || !FactionPerson(them))
                    return "that player isn't here";
                if (t.Of[who] == mine)
                    return $"{them.Name} is already in {t.Names[mine]}";
                if (t.Asked[who] == mine)
                    return FactionLetIn(p, them, mine);
                t.Invited[who] = mine;
                t.Version++;
                FactionTell(me, $"You invited {them.Name} to {t.Names[mine]}.");
                FactionTell(who, $"{p.Name} invited you to join {t.Names[mine]}: open the faction window to accept.");
                return null;
            }
            case FactionOp.Accept:
            {
                var id = r.U8();
                if (id == 0 || id >= FactionTable.Max || t.Invited[me] != id || t.Names[id].Length == 0)
                    return "that invite is gone";
                if (mine == id)
                    return $"you are already in {t.Names[id]}";
                FactionEnter(p, id);
                return null;
            }
            case FactionOp.Decline:
                if (t.Invited[me] == 0)
                    return null;
                t.Invited[me] = 0;
                t.Version++;
                return null;
            case FactionOp.Leave:
                if (mine == 0)
                    return "you are not in a faction";
                FactionTell(me, $"You left {t.Names[mine]}.");
                FactionLeft(p, t.Leave(me, Here));
                return null;
            case FactionOp.Remove:
            {
                var who = r.U8();
                if (mine == 0 || !t.May(me, FactionMay.Remove))
                    return $"your rank ({RankName(me)}) can't remove members";
                if (who == me)
                    return "to go, leave the faction";
                if (t.Of[who] != mine)
                    return "they aren't in your faction";
                if (t.RankOf(who) >= t.RankOf(me))
                    return $"{FactionMemberName(who)} is {RankName(who)}: you can only remove someone of a lower rank";
                var name = FactionMemberName(who);
                t.Leave(who, Here);
                FactionTell(who, $"You were removed from {t.Names[mine]}.");
                FactionTellAll(mine, $"{name} was removed from {t.Names[mine]}.");
                return null;
            }
            case FactionOp.GiveBody:
            {
                var body = r.U16();
                var to = r.U8();
                if (Constructs.Get(body) is not { } c)
                    return "that construct is gone";
                if (c.Owner != me)
                    return "only its owner can give it away";
                if (c.Wreck || c.Loose)
                    return "a wreck or a loose part belongs to nobody: there is nothing to give";
                if (to == me || mine == 0 || t.Of[to] != mine)
                    return "you can only give it to someone in your faction";
                if (!t.May(me, FactionMay.Give))
                    return $"your rank ({RankName(me)}) can't give constructs away";
                if (ConstructPlace.LimitRefusal(Constructs, to) is { } full)
                    return $"{FactionMemberName(to)} can't own another construct ({full})";
                c.Owner = to;
                Constructs.StructureDirty.Add(c.Id);
                FactionTell(me, $"You gave construct {c.Id} to {FactionMemberName(to)}.");
                FactionTell(to, $"{p.Name} gave you construct {c.Id}.");
                SandboxHands.Done.Add($"player {me} gave construct {c.Id} to {to}");
                return null;
            }
            case FactionOp.GiveHeld:
            {
                var to = r.U8();
                if (!p.Alive)
                    return null;
                ref var h = ref SandboxHands.Hand(me);
                if (to == me || mine == 0 || t.Of[to] != mine || !Players.TryGetValue(to, out var them) || !them.Alive)
                    return "you can only hand things to someone in your faction";
                if (h.Seated)
                    return "get out of the seat first";
                if (ConstructParts.Get(h.Held) is not { } def)
                    return "nothing in your hands to give";
                if (h.MagFor != 0 || Constructs.HoseHeldBy(me) >= 0)
                    return "that stays with you";
                if (Vector3.DistanceSquared(p.Position, them.Position) > FactionHandReach * FactionHandReach)
                    return $"{them.Name} is too far away to hand it to (within {FactionHandReach:0} m)";
                h.Held = ConstructParts.None;
                h.Edit = EditKind.None;
                var went = SandboxHands.Give(Constructs, to, def, them.Eye);
                FactionTell(me, $"You handed the {def.Name} to {them.Name}.");
                FactionTell(to, went == GiveResult.Dropped ? $"{p.Name} handed you the {def.Name}: your hands were full, it's at your feet." : $"{p.Name} handed you the {def.Name}.");
                SandboxHands.Done.Add($"player {me} handed {def.Name} to {to} ({went})");
                return null;
            }
            case FactionOp.Looks:
            {
                var colour = r.U32() & 0xFFFFFF;
                var tag = FactionTable.CleanTag(r.Text(16));
                var open = r.U8() != 0;
                if (mine == 0 || !t.May(me, FactionMay.Looks))
                    return $"your rank ({RankName(me)}) can't change the faction's colours";
                if (tag.Length == 0)
                    return "a tag needs 1 to 5 letters or digits";
                for (var id = 1; id < FactionTable.Max; id++)
                    if (id != mine && t.Names[id].Length > 0 && t.Tag[id] == tag)
                        return $"{t.Names[id]} already uses the tag [{tag}]";
                var opened = open && !t.Open[mine];
                (t.Colour[mine], t.Open[mine]) = (colour, open);
                t.SetTag(mine, tag);
                t.Version++;
                if (opened)
                    FactionTellAll(mine, $"{t.Names[mine]} is open: anyone may join from their faction window.");
                return null;
            }
            case FactionOp.Join:
            {
                var id = r.U8();
                if (id == 0 || id >= FactionTable.Max || t.Names[id].Length == 0)
                    return "that faction is gone";
                if (mine == id)
                    return $"you are already in {t.Names[id]}";
                if (t.Open[id] || t.Invited[me] == id)
                {
                    FactionEnter(p, id);
                    return null;
                }
                if (t.Asked[me] == id)
                    return $"you have already asked {t.Names[id]}";
                t.Asked[me] = id;
                t.Version++;
                FactionTell(me, $"You asked to join {t.Names[id]}. Its officers will see it in their faction window.");
                for (var k = 0; k < MaxPlayers; k++)
                    if (t.Of[k] == id && t.May((byte)k, FactionMay.Invite) && Players.ContainsKey((byte)k))
                        FactionTell((byte)k, $"{p.Name} asks to join {t.Names[id]}: answer in the faction window.");
                return null;
            }
            case FactionOp.Answer:
            {
                var who = r.U8();
                var yes = r.U8() != 0;
                if (mine == 0 || !t.May(me, FactionMay.Invite))
                    return $"your rank ({RankName(me)}) can't let people in";
                if (t.Asked[who] != mine || !Players.TryGetValue(who, out var them))
                    return "they aren't asking any more";
                if (yes)
                    return FactionLetIn(p, them, mine);
                t.Asked[who] = 0;
                t.Version++;
                FactionTell(who, $"{t.Names[mine]} turned you down.");
                return null;
            }
            case FactionOp.SetRank:
            {
                var who = r.U8();
                int rank = r.U8();
                if (mine == 0 || !t.May(me, FactionMay.Promote))
                    return $"your rank ({RankName(me)}) can't promote anyone";
                if (who == me || t.Of[who] != mine)
                    return "they aren't in your faction";
                var my = t.RankOf(me);
                if (t.RankOf(who) >= my)
                    return $"{FactionMemberName(who)} is {RankName(who)}: you can only move someone of a lower rank";
                if (rank >= my || rank >= FactionRanks.Player.Top)
                    return "you can only give ranks below your own";
                if (t.Rank[who] == rank)
                    return null;
                var up = rank > t.Rank[who];
                t.Rank[who] = (byte)rank;
                t.Version++;
                FactionTellAll(mine, $"{FactionMemberName(who)} is now {FactionRanks.Player.RankName(rank)} of {t.Names[mine]}{(up ? "." : " (by " + p.Name + ").")}");
                return null;
            }
            case FactionOp.MakeLeader:
            {
                var who = r.U8();
                if (mine == 0 || t.Leader[mine] != me)
                    return "only the leader can hand the faction over";
                if (who == me || t.Of[who] != mine)
                    return "they aren't in your faction";
                t.Leader[mine] = who;
                t.Rank[who] = (byte)FactionRanks.Player.Top;
                t.Rank[me] = (byte)(FactionRanks.Player.Top - 1);
                t.Version++;
                FactionTellAll(mine, $"{p.Name} handed {t.Names[mine]} to {FactionMemberName(who)}, who leads it now.");
                return null;
            }
            case FactionOp.Stance:
            {
                var other = r.U8();
                var stance = (sbyte)Math.Clamp((int)(sbyte)r.U8(), -1, 1);
                if (mine == 0 || !t.May(me, FactionMay.Relations))
                    return $"your rank ({RankName(me)}) can't speak for the faction";
                if (other == 0 || other == mine || other >= FactionTable.Max || t.Names[other].Length == 0)
                    return "that faction is gone";
                var was = t.Relation(mine, other);
                t.Stance[mine * FactionTable.Max + other] = stance;
                t.Version++;
                var now = t.Relation(mine, other);
                FactionTellAll(mine, stance switch
                {
                    < 0 => $"{p.Name} declared war on {t.Names[other]} for {t.Names[mine]}.",
                    > 0 when now == FactionTable.Relations.Allied => $"{t.Names[mine]} and {t.Names[other]} are allies.",
                    > 0 => $"{p.Name} offered {t.Names[other]} an alliance.",
                    _ => $"{p.Name} made {t.Names[mine]} neutral toward {t.Names[other]}{(now == FactionTable.Relations.War ? ", but they are still at war with us" : "")}.",
                });
                FactionTellAll(other, stance switch
                {
                    < 0 => $"{t.Names[mine]} declared war on {t.Names[other]}.",
                    > 0 when now == FactionTable.Relations.Allied => $"{t.Names[mine]} and {t.Names[other]} are allies.",
                    > 0 => $"{t.Names[mine]} offers {t.Names[other]} an alliance: accept it in the faction window.",
                    _ => $"{t.Names[mine]} is neutral toward {t.Names[other]} now.",
                });
                if (now != was)
                    StoryFactionRelation(t.Names[mine], t.Names[other], now, p); // (GameStoryFactions.cs)
                return null;
            }
        }
        return "unknown faction action";
    }

    /// <summary>A member's rank name.</summary>
    private string RankName(byte who) => FactionRanks.Player.RankName(FactionTable.RankOf(who));

    /// <summary><paramref name="p"/> joins faction <paramref name="id"/> (leaving their old one) at the joining rank.</summary>
    private void FactionEnter(Player p, byte id)
    {
        var t = FactionTable;
        var me = p.Id;
        if (t.Of[me] != 0)
            FactionLeft(p, t.Leave(me, Here));
        t.Of[me] = id;
        t.Rank[me] = FactionRanks.Player.Join;
        t.MemberName[me] = p.Name;
        t.Invited[me] = 0;
        t.Asked[me] = 0;
        t.Version++;
        FactionTellAll(id, $"{p.Name} joined {t.Names[id]} as {FactionRanks.Player.RankName(FactionRanks.Player.Join)}: what each of you owns is shared.");
        SandboxHands.Done.Add($"player {me} joined faction {id}");
    }

    /// <summary>An officer lets in someone who asked.</summary>
    private string? FactionLetIn(Player by, Player them, byte id)
    {
        FactionEnter(them, id);
        FactionTell(them.Id, $"{by.Name} let you into {FactionTable.Names[id]}.");
        return null;
    }

    /// <summary>Whether owner byte <paramref name="b"/> is a player who is here (a leader hands over to them first).</summary>
    private bool Here(byte b) => b < MaxPlayers && Players.ContainsKey(b);

    /// <summary>The others hear that <paramref name="p"/> left faction <paramref name="id"/>.</summary>
    private void FactionLeft(Player p, byte id)
    {
        if (id == 0)
            return;
        var t = FactionTable;
        if (t.Names[id].Length > 0)
            FactionTellAll(id, $"{p.Name} left {t.Names[id]}{(t.Leader[id] != 255 ? $"; {FactionMemberName(t.Leader[id])} leads it" : "")}.");
        SandboxHands.Done.Add($"player {p.Id} left faction {id}");
    }

    private void FactionTell(byte who, string text)
    {
        if (who < MaxPlayers)
            SandboxHands.Notices.Add((who, text));
    }

    private void FactionTellAll(byte id, string text)
    {
        var t = FactionTable;
        for (var k = 0; k < MaxPlayers; k++)
            if (t.Of[k] == id && Players.ContainsKey((byte)k))
                FactionTell((byte)k, text);
    }

    /// <summary>Server: a player leaves the game. A saved world keeps them in their faction while they're away (<paramref name="offline"/>: their record's byte); else they leave it.</summary>
    private void FactionPlayerGone(Player p, byte offline)
    {
        var t = FactionTable;
        t.Invited[p.Id] = 0;
        t.Asked[p.Id] = 0;
        if (t.Of[p.Id] == 0)
            return;
        if (offline != 255)
            t.Move(p.Id, offline, p.Name);
        else
            FactionLeft(p, t.Leave(p.Id, Here));
        t.Version++;
    }

    /// <summary>Server: a player is back in a saved world: their faction from when they were away.</summary>
    private void FactionPlayerBack(Player p, byte offline)
    {
        var t = FactionTable;
        if (offline != 255 && t.Of[offline] != 0)
            t.Move(offline, p.Id, p.Name);
    }

    /// <summary>Server, each tick (after the actions): the table to everyone when it changed.</summary>
    private void SandboxFactionsFlush()
    {
        if (_factionSent == FactionTable.Version)
            return;
        _factionSent = FactionTable.Version;
        if (Net.IsOffline)
            return;
        WriteFactions(SbW);
        Net.Broadcast(SbW.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: the table to one joiner.</summary>
    private void SendFactions(RakNetGUID to)
    {
        WriteFactions(SbW);
        Net.SendTo(to, SbW.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>
    /// <see cref="Msg.Factions"/>: u8 n, each (u8 id, text name, u8 leader, u32 colour, text tag, u8 flags: 1 open,
    /// 2 notorious; u8 s, s x (i8 the story faction's standing, u8 its technologies learnt, a bit each)); u8 m, each member
    /// (u8 owner byte, u8 faction, text name, u8 rank); u8 k, each invite (u8 player, u8 faction); u8 k, each ask (u8 player,
    /// u8 faction); u8 k, each stance (u8 a, u8 b, i8 stance); u8 s, each story faction's name; then each one's technologies
    /// (u8 n, n x text: protocol 202); u8 p, each player who knows something (u8 slot, s x u8 learnt, a bit each: protocol 269).
    /// </summary>
    private void WriteFactions(PacketWriter w)
    {
        var t = FactionTable;
        w.Begin(Msg.Factions);
        var story = Math.Min(t.StoryNames.Length, FactionTable.MaxStory);
        byte n = 0;
        for (var id = 1; id < FactionTable.Max; id++)
            if (t.Names[id].Length > 0)
                n++;
        w.U8(n);
        for (var id = 1; id < FactionTable.Max; id++)
            if (t.Names[id].Length > 0)
            {
                w.U8((byte)id).Str(t.Names[id]).U8(t.Leader[id]).U32(t.Colour[id]).Str(t.Tag[id]).U8((byte)((t.Open[id] ? 1 : 0) | (t.Notorious[id] ? 2 : 0))).U8((byte)story);
                for (var f = 0; f < story; f++)
                    w.U8((byte)t.StoryStanding[id * FactionTable.MaxStory + f]).U8(t.StoryLearnt[id * FactionTable.MaxStory + f]);
            }
        byte m = 0;
        for (var k = 0; k < 255; k++)
            if (t.Of[k] != 0)
                m++;
        w.U8(m);
        for (var k = 0; k < 255; k++)
            if (t.Of[k] != 0)
                w.U8((byte)k).U8(t.Of[k]).Str(FactionMemberName((byte)k)).U8(t.Rank[k]);
        WriteFactionPairs(w, t.Invited);
        WriteFactionPairs(w, t.Asked);
        byte st = 0;
        for (var k = 0; k < t.Stance.Length; k++)
            if (t.Stance[k] != 0)
                st++;
        w.U8(st);
        for (var k = 0; k < t.Stance.Length; k++)
            if (t.Stance[k] != 0)
                w.U8((byte)(k / FactionTable.Max)).U8((byte)(k % FactionTable.Max)).U8((byte)t.Stance[k]);
        w.U8((byte)story);
        for (var f = 0; f < story; f++)
            w.Str(t.StoryNames[f]);
        for (var f = 0; f < story; f++)
        {
            var techs = f < t.StoryTech.Length ? t.StoryTech[f] : [];
            var tn = Math.Min(techs.Length, 8);
            w.U8((byte)tn);
            for (var k = 0; k < tn; k++)
                w.Str(techs[k]);
        }
        byte pl = 0;
        for (var k = 0; k < 256; k++)
            if (PlayerKnowsAny(t, k, story))
                pl++;
        w.U8(pl);
        for (var k = 0; k < 256; k++)
            if (PlayerKnowsAny(t, k, story))
            {
                w.U8((byte)k);
                for (var f = 0; f < story; f++)
                    w.U8(t.PlayerLearnt[k * FactionTable.MaxStory + f]);
            }
    }

    private static bool PlayerKnowsAny(FactionTable t, int slot, int story)
    {
        for (var f = 0; f < story; f++)
            if (t.PlayerLearnt[slot * FactionTable.MaxStory + f] != 0)
                return true;
        return false;
    }

    private static void WriteFactionPairs(PacketWriter w, byte[] of)
    {
        byte i = 0;
        for (var k = 0; k < MaxPlayers; k++)
            if (of[k] != 0)
                i++;
        w.U8(i);
        for (var k = 0; k < MaxPlayers; k++)
            if (of[k] != 0)
                w.U8((byte)k).U8(of[k]);
    }

    /// <summary>Client: a <see cref="Msg.Factions"/> (after the id): the whole table again.</summary>
    internal void ReadFactions(ref PacketReader r)
    {
        if (IsServer || !r.Has(1))
            return;
        var t = FactionTable;
        t.Clear();
        int n = r.U8();
        for (var k = 0; k < n && r.Has(3); k++)
        {
            var id = r.U8();
            var name = FactionTable.CleanName(r.Text(64));
            var leader = r.U8();
            var colour = r.U32() & 0xFFFFFF;
            var tag = r.Text(16);
            var flags = r.U8();
            int s = r.U8();
            var ok = id is > 0 and < FactionTable.Max;
            for (var f = 0; f < s && r.Has(2); f++)
            {
                var v = (sbyte)r.U8();
                var learnt = r.U8();
                if (ok && f < FactionTable.MaxStory)
                    (t.StoryStanding[id * FactionTable.MaxStory + f], t.StoryLearnt[id * FactionTable.MaxStory + f]) = (v, learnt);
            }
            if (!ok)
                continue;
            (t.Names[id], t.Leader[id], t.Colour[id], t.Open[id], t.Notorious[id]) = (name, leader, colour, (flags & 1) != 0, (flags & 2) != 0);
            t.SetTag(id, tag);
        }
        int m = r.Has(1) ? r.U8() : 0;
        for (var k = 0; k < m && r.Has(4); k++)
        {
            var who = r.U8();
            var id = r.U8();
            var name = r.Text(32);
            var rank = r.U8();
            if (who != 255 && id < FactionTable.Max)
                (t.Of[who], t.MemberName[who], t.Rank[who]) = (id, name, rank);
        }
        ReadFactionPairs(ref r, t.Invited);
        ReadFactionPairs(ref r, t.Asked);
        int st = r.Has(1) ? r.U8() : 0;
        for (var k = 0; k < st && r.Has(3); k++)
        {
            var a = r.U8();
            var b = r.U8();
            var v = (sbyte)r.U8();
            if (a < FactionTable.Max && b < FactionTable.Max)
                t.Stance[a * FactionTable.Max + b] = (sbyte)Math.Clamp((int)v, -1, 1);
        }
        int sn = r.Has(1) ? r.U8() : 0;
        var names = new string[Math.Min(sn, FactionTable.MaxStory)];
        for (var f = 0; f < sn; f++)
        {
            var name = r.Text(64);
            if (f < names.Length)
                names[f] = name;
        }
        t.StoryNames = names;
        var techs = new string[names.Length][];
        for (var f = 0; f < sn; f++)
        {
            int tn = r.Has(1) ? r.U8() : 0;
            var list = new string[tn];
            for (var k = 0; k < tn; k++)
                list[k] = r.Has(1) ? r.Text(255) : "";
            if (f < techs.Length)
                techs[f] = list;
        }
        for (var f = 0; f < techs.Length; f++)
            techs[f] ??= [];
        t.StoryTech = techs;
        int pl = r.Has(1) ? r.U8() : 0;
        for (var k = 0; k < pl && r.Has(1); k++)
        {
            var slot = r.U8();
            for (var f = 0; f < sn && r.Has(1); f++)
            {
                var mask = r.U8();
                if (f < FactionTable.MaxStory)
                    t.PlayerLearnt[slot * FactionTable.MaxStory + f] = mask;
            }
        }
        ConstructAssembling.TaughtBy = t.TeacherOf;
        t.Version++;
    }

    private static void ReadFactionPairs(ref PacketReader r, byte[] of)
    {
        int i = r.Has(1) ? r.U8() : 0;
        for (var k = 0; k < i && r.Has(2); k++)
        {
            var who = r.U8();
            var id = r.U8();
            if (id < FactionTable.Max)
                of[who] = id;
        }
    }

    /// <summary>
    /// Server, loading a world: its factions (<see cref="WorldSaveWriter.FactionsTag"/>, after the players). Members are
    /// found by their keys and kept under their records' offline bytes until they arrive.
    /// </summary>
    private void WorldReadFactions(SaveReader r, WorldSession ws)
    {
        var t = FactionTable;
        var n = r.Count(4);
        for (var id = 0; id < n; id++)
        {
            var name = FactionTable.CleanName(r.Str());
            if (id is > 0 and < FactionTable.Max)
            {
                t.Names[id] = name;
                t.Colour[id] = FactionTable.ColourFrom(name); // (until the FCT2 section, which older saves don't have)
                t.SetTag((byte)id, FactionTable.TagFrom(name));
            }
        }
        var m = r.Count(18);
        for (var k = 0; k < m; k++)
        {
            var key = r.U64();
            var steam = r.U64();
            var id = r.U8();
            var leads = r.U8() != 0;
            if (id is 0 or >= FactionTable.Max || t.Names[id].Length == 0)
                continue;
            foreach (var rec in ws.Players)
                if (rec.Key == key && rec.SteamId == steam && rec.Offline != 255)
                {
                    t.Of[rec.Offline] = id;
                    t.MemberName[rec.Offline] = rec.Name;
                    t.Rank[rec.Offline] = (byte)Math.Min(1, FactionRanks.Player.Top - 1); // (members from before ranks: FCT2 says)
                    if (leads)
                        (t.Leader[id], t.Rank[rec.Offline]) = (rec.Offline, (byte)FactionRanks.Player.Top);
                    break;
                }
        }
        for (var id = 1; id < FactionTable.Max; id++)
            if (t.Names[id].Length > 0 && t.Count((byte)id) == 0)
                t.Names[id] = ""; // (nobody left in it)
            else if (t.Names[id].Length > 0 && t.Leader[id] == 255)
                for (var k = 0; k < 255; k++)
                    if (t.Of[k] == id)
                    {
                        t.Leader[id] = (byte)k;
                        break;
                    }
        t.Version++;
    }

    /// <summary>
    /// Server, loading a world: its factions' colours, tags, joining, stances and ranks (<see cref="WorldSaveWriter.FactionLooks"/>,
    /// after <see cref="WorldSaveWriter.FactionsTag"/>).
    /// </summary>
    private void WorldReadFactionLooks(SaveReader r, WorldSession ws)
    {
        var t = FactionTable;
        var n = r.Count(6);
        for (var id = 0; id < n; id++)
        {
            var colour = r.U32() & 0xFFFFFF;
            var tag = r.Str();
            var open = r.U8() != 0;
            if (id is > 0 and < FactionTable.Max && t.Names[id].Length > 0)
            {
                (t.Colour[id], t.Open[id]) = (colour, open);
                if (FactionTable.CleanTag(tag).Length > 0)
                    t.SetTag((byte)id, tag);
            }
        }
        var st = r.Count(3);
        for (var k = 0; k < st; k++)
        {
            var at = r.U16();
            var v = (sbyte)r.U8();
            if (at < t.Stance.Length)
                t.Stance[at] = (sbyte)Math.Clamp((int)v, -1, 1);
        }
        var m = r.Count(17);
        for (var k = 0; k < m; k++)
        {
            var key = r.U64();
            var steam = r.U64();
            var rank = r.U8();
            foreach (var rec in ws.Players)
                if (rec.Key == key && rec.SteamId == steam && rec.Offline != 255)
                {
                    if (t.Of[rec.Offline] != 0 && t.Leader[t.Of[rec.Offline]] != rec.Offline)
                        t.Rank[rec.Offline] = (byte)Math.Min((int)rank, FactionRanks.Player.Top - 1);
                    break;
                }
        }
        for (var k = 0; k < t.Stance.Length; k++)
            if (t.Names[k / FactionTable.Max].Length == 0 || t.Names[k % FactionTable.Max].Length == 0)
                t.Stance[k] = 0; // (a faction that ended)
        t.Version++;
    }

    // ---- The client's (or the host's own) requests ---------------------------------------------------------------

    /// <summary>Client (or the host itself): a faction action. The server decides; a refusal comes back as a notice.</summary>
    public void SandboxFaction(FactionOp op, int a = 0, int b = 0, string text = "")
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Faction).U8((byte)op);
        switch (op)
        {
            case FactionOp.Found:
                w.Str(FactionTable.CleanName(text));
                break;
            case FactionOp.Invite:
            case FactionOp.Accept:
            case FactionOp.Remove:
            case FactionOp.GiveHeld:
                w.U8((byte)a);
                break;
            case FactionOp.GiveBody:
                w.U16((ushort)a).U8((byte)b);
                break;
            case FactionOp.Looks:
                w.U32((uint)a).Str(FactionTable.CleanTag(text)).U8((byte)b);
                break;
            case FactionOp.Join:
            case FactionOp.MakeLeader:
                w.U8((byte)a);
                break;
            case FactionOp.Answer:
            case FactionOp.SetRank:
            case FactionOp.Stance:
                w.U8((byte)a).U8((byte)b);
                break;
        }
        if (IsServer)
        {
            if (Local is { } me)
            {
                var r = new PacketReader(w.Span[1..]);
                if (SandboxActGuarded(me, ref r) is { } why)
                    SandboxSay(why);
                SandboxNoticesFlush(); // (the host's own lines at once)
            }
            return;
        }
        Net.SendToServer(w.Span, PacketReliability.ReliableOrdered);
    }
}
