using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The talk panel (Program.StoryTalk.cs; docs/story/STORY.md "Talking to people"): opened with the use key on one of the
/// story's people, at the right of the screen over the world (it goes on, and so do they). It shows what they said to
/// you lately and four asks: hello, any news, trade what you hold (a trader takes it and gives you something of theirs),
/// goodbye. The host decides everything; their answers come back as their own words. Esc, Goodbye or walking off
/// closes it. Every line is ASCII; nothing allocates in a steady frame.
/// </summary>
public sealed partial class Ui
{
    private ushort _talkHeldFor = ushort.MaxValue;
    private string _talkTradeLabel = "", _talkTitle = "";
    private byte _talkTitleFor = 255;

    /// <summary>The panel for <paramref name="person"/>; returns what was asked this frame, or null.</summary>
    public TalkAsk? DrawStoryTalk(Game game, Player person, ushort held, double now)
    {
        TalkAsk? asked = null;
        if (_talkTitleFor != person.Id || !_talkTitle.EndsWith(person.Name, StringComparison.Ordinal))
            (_talkTitleFor, _talkTitle) = (person.Id, person.Name.ToUpperInvariant());
        if (held != _talkHeldFor)
        {
            _talkHeldFor = held;
            _talkTradeLabel = ConstructParts.Get(held) is { } d ? $"Trade your {d.Name}##talktrade" : "Trade (hold something to offer)##talktrade";
        }
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X - 24 * _scale, vp.WorkPos.Y + vp.WorkSize.Y * 0.45f), ImGuiCond.Always, new float2(1, 0.5f));
        ImGui.SetNextWindowSize(new float2(420 * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.85f);
        if (ImGui.Begin("##storytalk"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing))
        {
            ImGui.PushTextWrapPos(0);
            ImGuiSafe.TextColored(Orange, _talkTitle);
            ImGui.Separator();
            // What they said lately (to anyone near: you heard it).
            var chat = game.Chat;
            var from = chat.Count;
            for (int i = chat.Count - 1, found = 0; i >= 0 && found < 4 && now - chat[i].Time <= 90; i--)
                if (chat[i].From == person.Id && (chat[i].Flags & ChatFlags.Say) != 0)
                    (from, found) = (i, found + 1);
            if (from == chat.Count)
                ImGuiSafe.TextColored(Dim, "They look up at you."u8);
            ImGui.PushStyleColor(ImGuiCol.Text, Rgba(SayChat, 1f));
            for (var i = from; i < chat.Count; i++)
                if (chat[i].From == person.Id && (chat[i].Flags & ChatFlags.Say) != 0)
                    ImGuiSafe.TextWrapped(chat[i].Text);
            ImGui.PopStyleColor();
            ImGui.Separator();
            var w = new float2(-1, 28 * _scale);
            // A storylet's answers this person waits for (GameStoryStorylets.cs), first.
            if (game.TalkChoicesBody == person.Id && game.TalkChoiceCount > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Orange);
                for (var k = 0; k < game.TalkChoiceCount; k++)
                    if (ImGui.Button(game.TalkChoiceLabels[k], w))
                        asked = (TalkAsk)((int)TalkAsk.Choose + k);
                ImGui.PopStyleColor();
                ImGui.Separator();
            }
            if (ImGui.Button("Hello##talkhello"u8, w))
                asked = TalkAsk.Greet;
            if (ImGui.Button("Any news?##talknews"u8, w))
                asked = TalkAsk.News;
            ImGui.BeginDisabled(held == ConstructParts.None);
            if (ImGui.Button(_talkTradeLabel, w))
                asked = TalkAsk.Trade;
            ImGui.EndDisabled();
            if (ImGui.Button("Goodbye [Esc]##talkbye"u8, w))
                asked = TalkAsk.Bye;
            ImGui.PopTextWrapPos();
        }
        ImGui.End();
        return asked;
    }
}
