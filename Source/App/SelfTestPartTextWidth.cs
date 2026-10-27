namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Part text width (board item): part descriptions, flavour text and stat lines wrap at <see cref="Ui.PartTextWidth"/>
    /// instead of widening tooltips and panels. There is no ImGui frame in the headless run, so this wraps the longest
    /// texts greedily at the character count that width holds (about 8 px per character at UI scale 1) and checks that
    /// every line fits and no single word is wider than the wrap (a word that long would overflow it).
    /// </summary>
    private static void CheckPartTextWidth()
    {
        const float pxPerChar = 8f;
        var cols = (int)(Ui.PartTextWidth / pxPerChar);
        if (Ui.PartTextWidth < 320 || Ui.PartTextWidth > 480 || cols < 40 || cols > 60)
            Fail($"part text width {Ui.PartTextWidth} px = {cols} characters: should be about 40-60");
        var longest = "";
        var worstWord = "";
        void Check(string what, string text)
        {
            if (text.Length > longest.Length) longest = text;
            foreach (var para in text.Split('\n'))
            {
                var line = 0;
                foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (word.Length > worstWord.Length) worstWord = word;
                    if (word.Length > cols)
                        Fail($"part text: '{what}' has a {word.Length}-character word '{word}' that cannot wrap within {cols} characters");
                    line = line == 0 ? word.Length : line + 1 + word.Length > cols ? word.Length : line + 1 + word.Length;
                    if (line > cols)
                        Fail($"part text: '{what}' line over {cols} characters");
                }
            }
        }
        foreach (var p in Parts.All)
        {
            Check(p.Name + " flavour", p.Flavor);
            Check(p.Name + " summary", Ui.PartSummary(p));
        }
        Line($"Part text wraps at {Ui.PartTextWidth} px (~{cols} chars); longest text {longest.Length} chars wraps to ~{longest.Length / cols + 1} lines; longest word '{worstWord}' ({worstWord.Length})");
    }
}
