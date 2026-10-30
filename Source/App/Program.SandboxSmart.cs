namespace CallOfSlop;

/// <summary>
/// Smart rotation on this PC (board item dev-sandbox-smart-rotation-sr01; Constructs/SmartRotation.cs; docs/sandbox/CONSTRUCTS.md
/// "Smart rotation"): the aimed socket's turns ranked once when the aimed socket, the held part or the construct's parts
/// change (never per frame), the ghost starting at the best, R going back to it and, pressed at a guess, on to the next
/// best; what the player sets down is remembered, and a correction is saved in settings.txt. Off (the parts menu's
/// "Smart rotation"): the fixed order, as before. The server checks the variant sent exactly as before.
/// </summary>
public static unsafe partial class Program
{
    private static readonly int[] _sbRank = new int[Rot24.Count]; // the aimed socket's variants, best first (smart rotation on)
    private static readonly byte[] _sbRankRot = new byte[Rot24.Count];
    private static int _sbRankN, _sbRankPos;
    private static long _sbRankKey = -1;
    private static readonly byte[] _sbFree = new byte[Rot24.Count]; // free placement's turns, best first
    private static int _sbFreeN, _sbFreePos;
    private static ushort _sbFreeHeld = ushort.MaxValue;
    private static bool _sbSmartLoaded;

    /// <summary>The aimed socket's guess: ranked again only when the socket, the held part, the construct's part count or the setting change.</summary>
    private static void SandboxSmartGuess(Construct c, ConstructPartDef held, in ConstructGhost gh, int ghostKey)
    {
        SandboxSmartLoad();
        var smart = _settings.SmartRotation;
        var rkey = (long)(uint)ghostKey | (long)held.Id << 32 | (long)c.PartCount << 48 | (smart ? 1L << 62 : 0);
        if (rkey == _sbRankKey)
            return;
        const long countBits = 0xffffL << 48 & ~(1L << 62);
        var newSocket = _sbRankKey < 0 || ((rkey ^ _sbRankKey) & ~countBits) != 0; // (anything but the part count changed)
        _sbRankKey = rkey;
        _sbLastGhostKey = ghostKey;
        _sbRankN = smart ? SmartRotation.Rank(c, held, gh.Part, gh.Socket, SmartRotation.Memory, _sbRank, _sbRankRot) : 0;
        // A new socket or part starts at the guess; the same socket after the construct changed keeps a turn the player chose.
        if (newSocket || _sbRankPos >= 0 || _sbVariant < 0)
        {
            _sbRankPos = 0;
            _sbVariant = _sbRankN > 0 ? _sbRank[0] : gh.Variant;
        }
    }

    /// <summary>The saved corrections, into smart rotation's memory once.</summary>
    private static void SandboxSmartLoad()
    {
        if (_sbSmartLoaded)
            return;
        _sbSmartLoaded = true;
        SmartRotation.Memory.Load(_settings.SmartRotationLearned);
    }

    /// <summary>R on a socket: back to the guess; already at a guess, the next best (smart rotation off: the first that fits).</summary>
    private static void SandboxGuessAgain(int firstFit)
    {
        if (_sbRankN == 0)
        {
            _sbVariant = firstFit;
            return;
        }
        _sbRankPos = _sbRankPos >= 0 && _sbVariant == _sbRank[_sbRankPos] ? (_sbRankPos + 1) % _sbRankN : 0;
        _sbVariant = _sbRank[_sbRankPos];
    }

    /// <summary>A correction was learned: saved with the settings (rare: only when the player turns away from the guess).</summary>
    private static void SandboxLearn(bool learned)
    {
        _sbRankKey = -1; // (the next guess counts this placement)
        if (!learned)
            return;
        _settings.SmartRotationLearned = SmartRotation.Memory.Save();
        _settings.Save();
    }
}
